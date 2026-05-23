using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.FailShipment;

internal sealed class FailShipmentHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly ShippingMetrics _metrics;

    public FailShipmentHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task<FailShipmentOutcome> HandleAsync(Guid shipmentId, string reason)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return FailShipmentOutcome.NotFound();
        }

        var fromStatus = shipment.Status;
        var now = DateTime.UtcNow;

        if (!shipment.TryFail(reason, now, ShipmentStatusSource.Admin))
        {
            return FailShipmentOutcome.IllegalTransition(shipment.Status);
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            await _shipmentStore.SaveChangesAsync();

            return new Event[]
            {
                new ShipmentFailedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    shipment.CarrierKey,
                    shipment.TrackingNumber,
                    reason,
                    now),
                new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: (int?)fromStatus,
                    ToStatus: (int)shipment.Status,
                    OccurredAt: now),
            };
        });

        _metrics.RecordStatusChange(shipment.Status);

        return FailShipmentOutcome.Success(new ShipmentResponse(
            shipment.Id,
            shipment.OrderId,
            shipment.CustomerId,
            shipment.WarehouseId,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()));
    }
}

internal sealed record FailShipmentOutcome(
    FailShipmentOutcomeKind Kind,
    ShipmentResponse? Response,
    ShipmentStatus? CurrentStatus)
{
    public static FailShipmentOutcome Success(ShipmentResponse response)
        => new(FailShipmentOutcomeKind.Success, response, null);

    public static FailShipmentOutcome NotFound()
        => new(FailShipmentOutcomeKind.NotFound, null, null);

    public static FailShipmentOutcome IllegalTransition(ShipmentStatus currentStatus)
        => new(FailShipmentOutcomeKind.IllegalTransition, null, currentStatus);
}

internal enum FailShipmentOutcomeKind
{
    Success,
    NotFound,
    IllegalTransition,
}

using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.DeliverShipment;

internal sealed class DeliverShipmentHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly ShippingMetrics _metrics;

    public DeliverShipmentHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task<DeliverShipmentOutcome> HandleAsync(Guid shipmentId)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return DeliverShipmentOutcome.NotFound();
        }

        var fromStatus = shipment.Status;
        var createdAt = shipment.CreatedAt;
        var now = DateTime.UtcNow;

        if (!shipment.TryDeliver(now, ShipmentStatusSource.Admin))
        {
            return DeliverShipmentOutcome.IllegalTransition(shipment.Status);
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            await _shipmentStore.SaveChangesAsync();

            return new Event[]
            {
                new ShipmentDeliveredEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    shipment.CarrierKey,
                    shipment.TrackingNumber,
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
        _metrics.RecordTimeToDelivery(createdAt, now);

        return DeliverShipmentOutcome.Success(new ShipmentResponse(
            shipment.Id,
            shipment.OrderId,
            shipment.CustomerId,
            shipment.WarehouseId,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()));
    }
}

internal sealed record DeliverShipmentOutcome(
    DeliverShipmentOutcomeKind Kind,
    ShipmentResponse? Response,
    ShipmentStatus? CurrentStatus)
{
    public static DeliverShipmentOutcome Success(ShipmentResponse response)
        => new(DeliverShipmentOutcomeKind.Success, response, null);

    public static DeliverShipmentOutcome NotFound()
        => new(DeliverShipmentOutcomeKind.NotFound, null, null);

    public static DeliverShipmentOutcome IllegalTransition(ShipmentStatus currentStatus)
        => new(DeliverShipmentOutcomeKind.IllegalTransition, null, currentStatus);
}

internal enum DeliverShipmentOutcomeKind
{
    Success,
    NotFound,
    IllegalTransition,
}

using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.PickShipment;

internal sealed class PickShipmentHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly ShippingMetrics _metrics;

    public PickShipmentHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task<PickShipmentOutcome> HandleAsync(Guid shipmentId)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return PickShipmentOutcome.NotFound();
        }

        var fromStatus = shipment.Status;
        var now = DateTime.UtcNow;

        if (!shipment.TryPick(now, ShipmentStatusSource.Admin))
        {
            return PickShipmentOutcome.IllegalTransition(shipment.Status);
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            await _shipmentStore.SaveChangesAsync();

            return new Event[]
            {
                new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: fromStatus,
                    ToStatus: shipment.Status,
                    OccurredAt: now),
            };
        });

        _metrics.RecordStatusChange(shipment.Status);

        return PickShipmentOutcome.Success(new ShipmentResponse(
            shipment.Id,
            shipment.OrderId,
            shipment.CustomerId,
            shipment.WarehouseId,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()));
    }
}

internal sealed record PickShipmentOutcome(
    PickShipmentOutcomeKind Kind,
    ShipmentResponse? Response,
    ShipmentStatus? CurrentStatus)
{
    public static PickShipmentOutcome Success(ShipmentResponse response)
        => new(PickShipmentOutcomeKind.Success, response, null);

    public static PickShipmentOutcome NotFound()
        => new(PickShipmentOutcomeKind.NotFound, null, null);

    public static PickShipmentOutcome IllegalTransition(ShipmentStatus currentStatus)
        => new(PickShipmentOutcomeKind.IllegalTransition, null, currentStatus);
}

internal enum PickShipmentOutcomeKind
{
    Success,
    NotFound,
    IllegalTransition,
}

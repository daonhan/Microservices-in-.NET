using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.PackShipment;

internal sealed class PackShipmentHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly ShippingMetrics _metrics;

    public PackShipmentHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task<PackShipmentOutcome> HandleAsync(Guid shipmentId)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return PackShipmentOutcome.NotFound();
        }

        var fromStatus = shipment.Status;
        var now = DateTime.UtcNow;

        if (!shipment.TryPack(now, ShipmentStatusSource.Admin))
        {
            return PackShipmentOutcome.IllegalTransition(shipment.Status);
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

        return PackShipmentOutcome.Success(new ShipmentResponse(
            shipment.Id,
            shipment.OrderId,
            shipment.CustomerId,
            shipment.WarehouseId,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()));
    }
}

internal sealed record PackShipmentOutcome(
    PackShipmentOutcomeKind Kind,
    ShipmentResponse? Response,
    ShipmentStatus? CurrentStatus)
{
    public static PackShipmentOutcome Success(ShipmentResponse response)
        => new(PackShipmentOutcomeKind.Success, response, null);

    public static PackShipmentOutcome NotFound()
        => new(PackShipmentOutcomeKind.NotFound, null, null);

    public static PackShipmentOutcome IllegalTransition(ShipmentStatus currentStatus)
        => new(PackShipmentOutcomeKind.IllegalTransition, null, currentStatus);
}

internal enum PackShipmentOutcomeKind
{
    Success,
    NotFound,
    IllegalTransition,
}

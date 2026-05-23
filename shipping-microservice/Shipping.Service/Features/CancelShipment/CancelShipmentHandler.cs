using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Domain;
using Shipping.Service.Domain.Abstractions;
using Shipping.Service.Features.GetShipmentsByOrder;
using Shipping.Service.Infrastructure.Observability;

namespace Shipping.Service.Features.CancelShipment;

internal sealed class CancelShipmentHandler
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly ShippingMetrics _metrics;

    public CancelShipmentHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        ShippingMetrics metrics)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metrics = metrics;
    }

    public async Task<CancelShipmentOutcome> HandleAsync(Guid shipmentId, string? reason)
    {
        var shipment = await _shipmentStore.GetById(shipmentId);
        if (shipment is null)
        {
            return CancelShipmentOutcome.NotFound();
        }

        var fromStatus = shipment.Status;
        var now = DateTime.UtcNow;

        if (!shipment.TryCancel(now, ShipmentStatusSource.Admin, reason))
        {
            return CancelShipmentOutcome.IllegalTransition(shipment.Status);
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            await _shipmentStore.SaveChangesAsync();

            return new Event[]
            {
                new ShipmentCancelledEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    now,
                    reason),
                new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: fromStatus,
                    ToStatus: shipment.Status,
                    OccurredAt: now),
            };
        });

        _metrics.RecordStatusChange(shipment.Status);

        return CancelShipmentOutcome.Success(new ShipmentResponse(
            shipment.Id,
            shipment.OrderId,
            shipment.CustomerId,
            shipment.WarehouseId,
            shipment.Status.ToString(),
            shipment.CreatedAt,
            shipment.Lines.Select(l => new ShipmentLineDto(l.ProductId, l.Quantity)).ToList()));
    }
}

internal sealed record CancelShipmentOutcome(
    CancelShipmentOutcomeKind Kind,
    ShipmentResponse? Response,
    ShipmentStatus? CurrentStatus)
{
    public static CancelShipmentOutcome Success(ShipmentResponse response)
        => new(CancelShipmentOutcomeKind.Success, response, null);

    public static CancelShipmentOutcome NotFound()
        => new(CancelShipmentOutcomeKind.NotFound, null, null);

    public static CancelShipmentOutcome IllegalTransition(ShipmentStatus currentStatus)
        => new(CancelShipmentOutcomeKind.IllegalTransition, null, currentStatus);
}

internal enum CancelShipmentOutcomeKind
{
    Success,
    NotFound,
    IllegalTransition,
}

using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.Models;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class CancelShipmentCommandHandler : IEventHandler<CancelShipmentCommand>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public CancelShipmentCommandHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(CancelShipmentCommand command)
    {
        var shipments = await _shipmentStore.GetByOrder(command.OrderId);
        if (shipments.Count == 0)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var now = DateTime.UtcNow;
            var events = new List<Event>();
            var emittedReply = false;

            foreach (var shipment in shipments)
            {
                var fromStatus = shipment.Status;

                if (shipment.IsTerminal)
                {
                    if (!emittedReply && shipment.Status == ShipmentStatus.Cancelled)
                    {
                        events.Add(BuildReply(shipment, now, command));
                        emittedReply = true;
                    }
                    continue;
                }

                if (!shipment.TryCancel(now, ShipmentStatusSource.System, reason: command.Reason))
                {
                    continue;
                }

                events.Add(BuildReply(shipment, now, command));
                events.Add(new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: fromStatus,
                    ToStatus: shipment.Status,
                    OccurredAt: now)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                });
                emittedReply = true;
            }

            if (events.Count > 0)
            {
                await _shipmentStore.SaveChangesAsync();
            }

            return events;
        });
    }

    private static ShipmentCancelledEvent BuildReply(
        Shipment shipment,
        DateTime occurredAt,
        CancelShipmentCommand command) =>
        new(shipment.Id, shipment.OrderId, shipment.CustomerId, occurredAt, command.Reason)
        {
            CorrelationId = command.CorrelationId,
            CausationId = command.Id,
            SagaId = command.SagaId,
        };
}

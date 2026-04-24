using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Microsoft.EntityFrameworkCore;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.Models;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxStore _outboxStore;

    public OrderCancelledEventHandler(
        IShipmentStore shipmentStore,
        IOutboxStore outboxStore)
    {
        _shipmentStore = shipmentStore;
        _outboxStore = outboxStore;
    }

    public async Task Handle(OrderCancelledEvent @event)
    {
        var shipments = await _shipmentStore.GetByOrder(@event.OrderId);
        if (shipments.Count == 0)
        {
            return;
        }

        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var now = DateTime.UtcNow;
            var cancelledAny = false;

            foreach (var shipment in shipments)
            {
                var fromStatus = shipment.Status;

                if (!shipment.TryCancel(now, ShipmentStatusSource.System, reason: "Order cancelled"))
                {
                    continue;
                }

                cancelledAny = true;

                await _outboxStore.AddOutboxEvent(new ShipmentCancelledEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    now,
                    Reason: "Order cancelled"));

                await _outboxStore.AddOutboxEvent(new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: fromStatus,
                    ToStatus: shipment.Status,
                    OccurredAt: now));
            }

            if (cancelledAny)
            {
                await _shipmentStore.SaveChangesAsync();
            }

            scope.Complete();
        });
    }
}

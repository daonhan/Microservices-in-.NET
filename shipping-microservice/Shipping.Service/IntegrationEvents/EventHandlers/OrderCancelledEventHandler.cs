using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.Models;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxStore _outboxStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public OrderCancelledEventHandler(
        IShipmentStore shipmentStore,
        IOutboxStore outboxStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _shipmentStore = shipmentStore;
        _outboxStore = outboxStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(OrderCancelledEvent @event)
    {
        var shipments = await _shipmentStore.GetByOrder(@event.OrderId);
        if (shipments.Count == 0)
        {
            return;
        }

        await _outboxUnitOfWork.ExecuteAsync(_outboxStore.CreateExecutionStrategy(), async () =>
        {
            var now = DateTime.UtcNow;
            var events = new List<Event>();

            foreach (var shipment in shipments)
            {
                var fromStatus = shipment.Status;

                if (!shipment.TryCancel(now, ShipmentStatusSource.System, reason: "Order cancelled"))
                {
                    continue;
                }

                events.Add(new ShipmentCancelledEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    now,
                    Reason: "Order cancelled"));

                events.Add(new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: fromStatus,
                    ToStatus: shipment.Status,
                    OccurredAt: now));
            }

            if (events.Count > 0)
            {
                await _shipmentStore.SaveChangesAsync();
            }

            return events;
        });
    }
}

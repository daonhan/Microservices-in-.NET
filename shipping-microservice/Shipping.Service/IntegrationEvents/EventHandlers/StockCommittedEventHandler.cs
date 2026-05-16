using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Shipping.Service.Infrastructure.Data;
using Shipping.Service.Models;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class StockCommittedEventHandler : IEventHandler<StockCommittedEvent>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public StockCommittedEventHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(StockCommittedEvent @event)
    {
        if (@event.Items is null || @event.Items.Count == 0)
        {
            return;
        }

        var customerId = await _shipmentStore.TryGetOrderCustomer(@event.OrderId);
        if (customerId is null)
        {
            // OrderConfirmedEvent has not been observed yet. Phase 1 tracer bullet
            // relies on OrderConfirmedEvent preceding StockCommittedEvent (Order
            // service publishes confirmation after Inventory commits). A later
            // phase can add a retry/coordinator if observed ordering proves racy.
            return;
        }

        var lines = @event.Items
            .Select(i => new CreateShipmentLine(i.ProductId, i.WarehouseId, i.Quantity))
            .ToList();

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _shipmentStore.CreateShipmentsForOrder(@event.OrderId, customerId, lines);

            if (!result.Created)
            {
                return [];
            }

            var events = new List<Event>();

            foreach (var shipment in result.Shipments)
            {
                var lineItems = shipment.Lines
                    .Select(l => new ShipmentLineItem(l.ProductId, l.Quantity))
                    .ToList();

                events.Add(new ShipmentCreatedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    shipment.CustomerId,
                    shipment.WarehouseId,
                    lineItems));

                events.Add(new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: null,
                    ToStatus: ShipmentStatus.Pending,
                    OccurredAt: shipment.CreatedAt));
            }

            return events;
        });
    }
}

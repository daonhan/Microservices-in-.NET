using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.IntegrationEvents.Commands;
using Shipping.Service.Contracts.Integration;
using Shipping.Service.Infrastructure.Data;

namespace Shipping.Service.IntegrationEvents.EventHandlers;

internal class CreateShipmentCommandHandler : IEventHandler<CreateShipmentCommand>
{
    private readonly IShipmentStore _shipmentStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public CreateShipmentCommandHandler(
        IShipmentStore shipmentStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _shipmentStore = shipmentStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task Handle(CreateShipmentCommand command)
    {
        if (command.Items is null || command.Items.Count == 0)
        {
            return;
        }

        var customerId = await _shipmentStore.TryGetOrderCustomer(command.OrderId);
        if (customerId is null)
        {
            // OrderConfirmedEvent has not been observed yet — redelivery resolves the race.
            return;
        }

        var lines = command.Items
            .Select(i => new CreateShipmentLine(i.ProductId, i.WarehouseId, i.Quantity))
            .ToList();

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _shipmentStore.CreateShipmentsForOrder(command.OrderId, customerId, lines);

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
                    lineItems)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                });

                events.Add(new ShipmentStatusChangedEvent(
                    shipment.Id,
                    shipment.OrderId,
                    FromStatus: null,
                    ToStatus: Domain.ShipmentStatus.Pending,
                    OccurredAt: shipment.CreatedAt)
                {
                    CorrelationId = command.CorrelationId,
                    CausationId = command.Id,
                    SagaId = command.SagaId,
                });
            }

            return events;
        });
    }
}

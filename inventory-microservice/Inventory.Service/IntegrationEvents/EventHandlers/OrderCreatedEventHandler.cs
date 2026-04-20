using System.Globalization;
using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderCreatedEventHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxStore _outboxStore;

    public OrderCreatedEventHandler(IInventoryStore inventoryStore, IOutboxStore outboxStore)
    {
        _inventoryStore = inventoryStore;
        _outboxStore = outboxStore;
    }

    public async Task Handle(OrderCreatedEvent @event)
    {
        if (@event.Items is null || @event.Items.Count == 0)
        {
            return;
        }

        var lines = @event.Items
            .Select(i => new ReserveLine(int.Parse(i.ProductId, CultureInfo.InvariantCulture), i.Quantity))
            .ToList();

        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var result = await _inventoryStore.Reserve(@event.OrderId, lines);

            if (result.AlreadyProcessed)
            {
                scope.Complete();
                return;
            }

            if (!result.Reserved)
            {
                var failed = result.FailedLines
                    .Select(l => new FailedItem(l.ProductId, l.Requested, l.Available))
                    .ToList();

                await _outboxStore.AddOutboxEvent(new StockReservationFailedEvent(@event.OrderId, failed));

                scope.Complete();
                return;
            }

            var published = result.Lines
                .Select(l => new ReservedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            await _outboxStore.AddOutboxEvent(new StockReservedEvent(@event.OrderId, published));

            scope.Complete();
        });
    }
}

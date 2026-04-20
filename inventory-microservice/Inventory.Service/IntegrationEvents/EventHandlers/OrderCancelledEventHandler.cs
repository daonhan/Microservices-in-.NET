using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderCancelledEventHandler : IEventHandler<OrderCancelledEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxStore _outboxStore;

    public OrderCancelledEventHandler(IInventoryStore inventoryStore, IOutboxStore outboxStore)
    {
        _inventoryStore = inventoryStore;
        _outboxStore = outboxStore;
    }

    public async Task Handle(OrderCancelledEvent @event)
    {
        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var result = await _inventoryStore.ReleaseReservations(@event.OrderId);

            if (!result.Released || result.AlreadyProcessed)
            {
                scope.Complete();
                return;
            }

            var published = result.Lines
                .Select(l => new ReleasedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            await _outboxStore.AddOutboxEvent(new StockReleasedEvent(@event.OrderId, published));

            scope.Complete();
        });
    }
}

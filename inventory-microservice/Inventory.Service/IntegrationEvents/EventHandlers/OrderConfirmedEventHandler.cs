using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxStore _outboxStore;

    public OrderConfirmedEventHandler(IInventoryStore inventoryStore, IOutboxStore outboxStore)
    {
        _inventoryStore = inventoryStore;
        _outboxStore = outboxStore;
    }

    public async Task Handle(OrderConfirmedEvent @event)
    {
        await _outboxStore.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            using var scope = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled);

            var result = await _inventoryStore.CommitReservations(@event.OrderId);

            if (!result.Committed)
            {
                return;
            }

            if (result.AlreadyProcessed)
            {
                scope.Complete();
                return;
            }

            var published = result.Lines
                .Select(l => new CommittedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            await _outboxStore.AddOutboxEvent(new StockCommittedEvent(@event.OrderId, published));

            scope.Complete();
        });
    }
}

using System.Transactions;
using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxStore _outboxStore;
    private readonly MetricFactory _metricFactory;

    public OrderConfirmedEventHandler(IInventoryStore inventoryStore, IOutboxStore outboxStore, MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxStore = outboxStore;
        _metricFactory = metricFactory;
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

            foreach (var _ in result.Lines)
            {
                _metricFactory.Counter("stock-movements", "movements")
                    .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Commit)));
            }

            await _outboxStore.AddOutboxEvent(new StockCommittedEvent(@event.OrderId, published));

            scope.Complete();
        });
    }
}

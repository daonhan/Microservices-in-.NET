using ECommerce.Shared.Infrastructure.EventBus.Abstractions;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Infrastructure.Data;
using Inventory.Service.Models;

namespace Inventory.Service.IntegrationEvents.EventHandlers;

internal class OrderConfirmedEventHandler : IEventHandler<OrderConfirmedEvent>
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MetricFactory _metricFactory;

    public OrderConfirmedEventHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metricFactory = metricFactory;
    }

    public async Task Handle(OrderConfirmedEvent @event)
    {
        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            var result = await _inventoryStore.CommitReservations(@event.OrderId);

            if (result.AlreadyProcessed)
            {
                return [];
            }

            if (!result.Committed)
            {
                throw new InvalidOperationException(
                    $"Commit failed for order {@event.OrderId} — rolling back transaction.");
            }

            var published = result.Lines
                .Select(l => new CommittedItem(l.ProductId, l.WarehouseId, l.Quantity))
                .ToList();

            foreach (var _ in result.Lines)
            {
                _metricFactory.Counter("stock-movements", "movements")
                    .Add(1, new KeyValuePair<string, object?>("movement_type", nameof(MovementType.Commit)));
            }

            return [new StockCommittedEvent(@event.OrderId, published)];
        });
    }
}

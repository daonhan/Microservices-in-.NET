using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using ECommerce.Shared.Observability.Metrics;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain;
using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.Features.Restock;

internal sealed class RestockHandler
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;
    private readonly MetricFactory _metricFactory;

    public RestockHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork,
        MetricFactory metricFactory)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
        _metricFactory = metricFactory;
    }

    public async Task<RestockResponse?> HandleAsync(int productId, RestockRequest request)
    {
        RestockResult? result = null;

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            result = await _inventoryStore.Restock(productId, request.Quantity);

            if (result is null)
            {
                return [];
            }

            var events = new List<Event>
            {
                new StockAdjustedEvent(
                    productId,
                    result.WarehouseId,
                    request.Quantity,
                    result.NewOnHand),
            };

            var lowStock = StockLevelMonitor.TryLowStockCrossing(
                productId,
                result.WarehouseId,
                result.AvailableBefore,
                result.AvailableAfter,
                result.Threshold,
                result.Threshold);
            if (lowStock is not null)
            {
                events.Add(new LowStockEvent(
                    lowStock.ProductId,
                    lowStock.WarehouseId,
                    lowStock.AvailableAfter,
                    lowStock.ThresholdAfter));
            }

            var depleted = StockLevelMonitor.TryDepletedCrossing(
                productId,
                result.WarehouseId,
                result.AvailableBefore,
                result.AvailableAfter);
            if (depleted is not null)
            {
                events.Add(new StockDepletedEvent(depleted.ProductId, depleted.WarehouseId));
                _metricFactory.Counter("stock-depleted", "events").Add(1);
            }

            return events;
        });

        if (result is null)
        {
            return null;
        }

        return new RestockResponse(productId, result.WarehouseId, result.NewOnHand);
    }
}

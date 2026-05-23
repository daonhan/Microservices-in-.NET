using ECommerce.Shared.Infrastructure.EventBus;
using ECommerce.Shared.Infrastructure.Outbox;
using Inventory.Service.Contracts.Integration;
using Inventory.Service.Domain;
using Inventory.Service.Domain.Abstractions;

namespace Inventory.Service.Features.SetThreshold;

internal sealed class SetThresholdHandler
{
    private readonly IInventoryStore _inventoryStore;
    private readonly IOutboxUnitOfWork _outboxUnitOfWork;

    public SetThresholdHandler(
        IInventoryStore inventoryStore,
        IOutboxUnitOfWork outboxUnitOfWork)
    {
        _inventoryStore = inventoryStore;
        _outboxUnitOfWork = outboxUnitOfWork;
    }

    public async Task<SetThresholdResponse?> HandleAsync(int productId, SetThresholdRequest request)
    {
        SetThresholdResult? result = null;

        await _outboxUnitOfWork.ExecuteAsync(async () =>
        {
            result = await _inventoryStore.SetThreshold(productId, request.Threshold);

            if (result is null)
            {
                return [];
            }

            var lowStock = StockLevelMonitor.TryLowStockCrossing(
                productId,
                result.WarehouseId,
                result.Available,
                result.Available,
                result.ThresholdBefore,
                result.ThresholdAfter);

            if (lowStock is null)
            {
                return [];
            }

            return new List<Event>
            {
                new LowStockEvent(
                    lowStock.ProductId,
                    lowStock.WarehouseId,
                    lowStock.AvailableAfter,
                    lowStock.ThresholdAfter),
            };
        });

        if (result is null)
        {
            return null;
        }

        return new SetThresholdResponse(productId, result.ThresholdAfter);
    }
}

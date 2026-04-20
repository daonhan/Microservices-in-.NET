using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record LowStockEvent(
    int ProductId,
    int WarehouseId,
    int Available,
    int Threshold) : Event;

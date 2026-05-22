using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.Contracts.Integration;

public record LowStockEvent(
    int ProductId,
    int WarehouseId,
    int Available,
    int Threshold) : Event;

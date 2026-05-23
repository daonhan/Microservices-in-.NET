using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.Contracts.Integration;

public record StockDepletedEvent(
    int ProductId,
    int WarehouseId) : Event;

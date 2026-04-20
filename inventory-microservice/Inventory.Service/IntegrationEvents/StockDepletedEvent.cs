using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record StockDepletedEvent(
    int ProductId,
    int WarehouseId) : Event;

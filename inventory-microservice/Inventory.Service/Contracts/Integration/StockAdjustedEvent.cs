using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.Contracts.Integration;

public record StockAdjustedEvent(
    int ProductId,
    int WarehouseId,
    int Quantity,
    int NewOnHand) : Event;

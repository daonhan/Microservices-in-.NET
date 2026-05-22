using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.Contracts.Integration;

public record ReleasedItem(int ProductId, int WarehouseId, int Quantity);

public record StockReleasedEvent(Guid OrderId, IReadOnlyList<ReleasedItem> Items) : Event;

using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record ReservedItem(int ProductId, int WarehouseId, int Quantity);

public record StockReservedEvent(Guid OrderId, IReadOnlyList<ReservedItem> Items) : Event;

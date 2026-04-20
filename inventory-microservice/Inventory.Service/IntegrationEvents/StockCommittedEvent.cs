using ECommerce.Shared.Infrastructure.EventBus;

namespace Inventory.Service.IntegrationEvents;

public record CommittedItem(int ProductId, int WarehouseId, int Quantity);

public record StockCommittedEvent(Guid OrderId, IReadOnlyList<CommittedItem> Items) : Event;

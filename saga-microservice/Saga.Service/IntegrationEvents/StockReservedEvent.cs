using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record ReservedItem(int ProductId, int WarehouseId, int Quantity);

public record StockReservedEvent(
    Guid OrderId,
    IReadOnlyList<ReservedItem> Items,
    decimal Amount = 0m,
    string Currency = "USD") : Event;

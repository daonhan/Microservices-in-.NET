using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record ReleasedItem(int ProductId, int WarehouseId, int Quantity);

public record StockReleasedEvent(Guid OrderId, IReadOnlyList<ReleasedItem> Items) : Event;

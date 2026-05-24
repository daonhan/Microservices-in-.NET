using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.Contracts.Integration.InboundEvents;

public record CommittedItem(int ProductId, int WarehouseId, int Quantity);

public record StockCommittedEvent(Guid OrderId, IReadOnlyList<CommittedItem> Items) : Event;

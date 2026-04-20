using ECommerce.Shared.Infrastructure.EventBus;

namespace Order.Service.IntegrationEvents.Events;

public record FailedItem(int ProductId, int Requested, int Available);

public record StockReservationFailedEvent(Guid OrderId, IReadOnlyList<FailedItem> FailedItems) : Event;

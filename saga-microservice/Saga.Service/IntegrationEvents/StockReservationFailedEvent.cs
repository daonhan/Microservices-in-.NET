using ECommerce.Shared.Infrastructure.EventBus;

namespace Saga.Service.IntegrationEvents;

public record FailedItem(int ProductId, int Requested, int Available);

public record StockReservationFailedEvent(Guid OrderId, IReadOnlyList<FailedItem> FailedItems) : Event;
